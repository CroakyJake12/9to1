using Avalonia.Controls;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

/// <summary>
/// Drives the icon surface through the rendered CUI controls rather than through the view model's command
/// dispatcher, which is what the earlier picker tests did. That difference is the point: in the running
/// application clicking an emoji cell changed nothing while Shuffle - the same popover, a parameterless
/// command - stored an emoji and recorded it, and a command-level test could not see either the failure or
/// the fix, because it never exercised the authored action wiring.
/// </summary>
[Collection(BoardSessionTestCollection.Name)]
public sealed class IconPickerSurfaceClickTests
{
    [Fact]
    public void Clicking_an_emoji_cell_in_the_rendered_picker_stores_it_on_the_page()
    {
        var surface = BoardsRenderedSurface.Open("cell");
        try
        {
            var viewModel = surface.ViewModel;
            var session = viewModel.Session;
            var section = session.Document.Sections[0];
            var page = section.Pages[0];

            surface.EnableEdit();
            TestUiThread.Run(() =>
            {
                viewModel.OpenPageIconPicker(section.Id, page.Id);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });
            Assert.True(viewModel.Get("IsSectionIconPickerOpen") as bool?, "the authored picker should be open");

            var emojiButtons = surface.EmojiGridButtons();
            Assert.NotEmpty(emojiButtons);

            surface.Click(emojiButtons[0]);

            var after = session.Document.Sections[0].Pages[0];
            Assert.Equal(HavenRichSectionIcons.Emoji, after.IconKey);
            Assert.False(string.IsNullOrEmpty(after.IconEmoji),
                "the clicked cell's emoji should be stored; an empty result means the authored action did not carry the item value");
        }
        finally
        {
            surface.Close();
        }
    }

    /// <summary>
    /// The reference design puts the page's icon to the left of its title, and that slot is where the owner
    /// opens the picker while reading the page they are on. Both halves matter: the slot must exist in the
    /// rendered header (an authored element the runtime never realises is invisible to the owner), and its
    /// activation must target the displayed page, not whichever one the tree happens to highlight.
    /// </summary>
    [Fact]
    public void The_page_header_icon_slot_opens_the_shared_picker_for_the_displayed_page()
    {
        var surface = BoardsRenderedSurface.Open("header");
        try
        {
            var viewModel = surface.ViewModel;
            var session = viewModel.Session;
            var page = session.Document.Sections[0].Pages[0];

            surface.EnableEdit();

            var slot = surface.Find<Button>("PageIconButton");
            Assert.NotNull(slot);
            Assert.False(string.IsNullOrWhiteSpace(surface.AccessibleName(slot!)),
                "the header icon slot needs a name that says it is this page's icon");
            Assert.Contains("icon", surface.AccessibleName(slot!), StringComparison.OrdinalIgnoreCase);

            surface.Click(slot!);

            Assert.True(viewModel.Get("IsSectionIconPickerOpen") as bool?,
                "the header slot should open the same shared picker the tree rows use");
            Assert.Equal("page", viewModel.Get("IconPickerTargetKind") as string);

            var emojiButtons = surface.EmojiGridButtons();
            Assert.NotEmpty(emojiButtons);
            surface.Click(emojiButtons[0]);

            var after = session.Document.Sections[0].Pages[0];
            Assert.Equal(HavenRichSectionIcons.Emoji, after.IconKey);
            Assert.False(string.IsNullOrEmpty(after.IconEmoji));

            // Applying an icon through the picker has to update the header it was opened from, otherwise the
            // owner sees no confirmation that the page they are standing on changed.
            Assert.True(viewModel.Get("PageIconIsEmoji") as bool?,
                "the header should switch to the emoji form once an emoji is chosen");
            Assert.Equal(after.IconEmoji, viewModel.Get("PageIconGlyph") as string);
            var glyph = surface.Find<TextBlock>("PageIconGlyph");
            Assert.NotNull(glyph);
            Assert.True(surface.Shown(glyph!), "the chosen emoji should be drawn in the header");
            Assert.Equal(after.IconEmoji, surface.TextOf(glyph!));
        }
        finally
        {
            surface.Close();
        }
    }

    /// <summary>
    /// The icon belongs to the page, not to the editing state, so it stays drawn in View mode; only its
    /// activation is withheld there. This is the same rule the tree rows follow, asserted on the rendered
    /// header rather than in the projection, because the rule is enforced by authored bindings.
    /// </summary>
    [Fact]
    public void The_page_header_icon_stays_displayed_but_inert_outside_edit_mode()
    {
        var surface = BoardsRenderedSurface.Open("viewmode");
        try
        {
            var viewModel = surface.ViewModel;
            var session = viewModel.Session;
            var section = session.Document.Sections[0];
            var page = section.Pages[0];

            surface.EnableEdit();
            TestUiThread.Run(() =>
            {
                viewModel.OpenPageIconPicker(section.Id, page.Id);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });
            var emojiButtons = surface.EmojiGridButtons();
            Assert.NotEmpty(emojiButtons);
            surface.Click(emojiButtons[0]);

            surface.DisableEdit();
            Assert.False(viewModel.IsEditMode, "the surface should be back in View mode");

            var slot = surface.Find<Button>("PageIconButton");
            Assert.NotNull(slot);
            Assert.False(surface.Enabled(slot!),
                "View mode must not offer to change the page's icon, but the icon itself stays visible");
            var glyph = surface.Find<TextBlock>("PageIconGlyph");
            Assert.NotNull(glyph);
            Assert.True(surface.Shown(glyph!), "a chosen icon belongs to the page and is still shown in View mode");
        }
        finally
        {
            surface.Close();
        }
    }

    /// <summary>
    /// The Board menu is the entry point for the Board's icon, so it has to exist in the rendered surface and
    /// its row has to reach the shared popover with the Board as target. A stored field with a menu row that
    /// does nothing is exactly what canonical v2 line 9397 forbids.
    /// </summary>
    [Fact]
    public void The_Board_menu_offers_the_icon_action_and_it_targets_the_Board()
    {
        var surface = BoardsRenderedSurface.Open("boardmenu");
        try
        {
            var viewModel = surface.ViewModel;
            surface.EnableEdit();

            var row = surface.Find<Control>("BoardIconMenuItem");
            Assert.NotNull(row);
            Assert.True(surface.Enabled(row!), "the Board's icon can be set while a Board is open in Edit mode");
            Assert.Contains("icon", surface.AccessibleName(row!), StringComparison.OrdinalIgnoreCase);

            surface.Click(row!);

            Assert.True(viewModel.Get("IsSectionIconPickerOpen") as bool?,
                "the Board menu row must open the same popover the tree rows and the page header use");
            Assert.Equal("board", viewModel.Get("IconPickerTargetKind") as string);
            Assert.Contains("Board appearance", viewModel.Get("SectionIconPickerTitle") as string);
        }
        finally
        {
            surface.Close();
        }
    }

    /// <summary>
    /// The board switcher is where the owner sees which Board they are in, so the Board's own mark belongs
    /// there. An owner-chosen emoji is carried as a text mark - an image cannot hold a colour emoji - and the
    /// mark stays visible in View mode, because it identifies the Board rather than an editing state.
    /// </summary>
    [Fact]
    public void The_board_switcher_shows_the_Board_chosen_mark()
    {
        var surface = BoardsRenderedSurface.Open("switcher");
        try
        {
            var viewModel = surface.ViewModel;
            surface.EnableEdit();
            TestUiThread.Run(() =>
            {
                viewModel.DispatchAsync("OpenBoardIconPicker", null).GetAwaiter().GetResult();
                viewModel.DispatchAsync("ApplyEmojiIconChoice", "📚").GetAwaiter().GetResult();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });
            Assert.Equal(HavenRichSectionIcons.Emoji, viewModel.Session.Document.IconKey);

            var item = surface.Find<CakeOS.Cui.Runtime.CuiMenuItem>("BoardSwitcher");
            Assert.NotNull(item);
            Assert.True(surface.HasGlyph(item!),
                "the switcher has to carry the mark the Board was given");
            Assert.Equal("📚", surface.GlyphOf(item!));
            Assert.True(surface.DrawsText(item!, "📚"), "the mark is drawn, not merely stored");

            // The native pass caught this the way a headless test would not: the first version of the template
            // hid the label whenever a mark existed, so the switcher showed the emoji and lost the Board's
            // name - the one thing that control exists to tell the owner.
            var title = viewModel.Get("BoardMenuTitle") as string;
            Assert.False(string.IsNullOrWhiteSpace(title), "the switcher keeps naming the Board");
            Assert.True(surface.DrawsText(item!, title!),
                "a mark sits beside the Board's name, it does not replace it");

            // View mode keeps the identity mark and only withdraws the editing routes.
            surface.DisableEdit();
            Assert.False(viewModel.IsEditMode);
            Assert.True(surface.HasGlyph(item!),
                "a Board mark belongs to the Board, not to the editing state");
        }
        finally
        {
            surface.Close();
        }
    }

    /// <summary>
    /// A page with a vector identity icon publishes a catalog reference (icon:file, or icon:file|#RRGGBBAA
    /// when the owner has tinted it) rather than a media object, because the view model that publishes it can
    /// be built on a background thread while a save or reload is in flight. The reference is only useful if
    /// the surface really turns it into a drawing, so this asserts both halves: what is published, and what
    /// the rendered header holds after resolution.
    /// </summary>
    [Fact]
    public void The_header_vector_icon_publishes_a_catalog_reference_and_resolves_it_on_the_surface()
    {
        var surface = BoardsRenderedSurface.Open("vector");
        try
        {
            var viewModel = surface.ViewModel;
            var page = viewModel.Session.Document.Sections[0].Pages[0];
            Assert.Equal($"icon:{page.IconKey}", viewModel.Get("PageIconSource") as string);

            var image = surface.Find<Image>("PageIconImage");
            Assert.NotNull(image);
            Assert.True(surface.Shown(image!), "a page's vector identity icon is shown in the header in both modes");
            Assert.NotNull(surface.SourceOf(image!));

            var tinted = surface.ResolveVector("icon:file|#FFFF918A", image!);
            Assert.NotNull(tinted);
            Assert.NotSame(surface.SourceOf(image!), tinted);

            var toolbar = surface.ResolveVector("icon:undo", image!);
            Assert.NotNull(toolbar);
        }
        finally
        {
            surface.Close();
        }
    }
}
